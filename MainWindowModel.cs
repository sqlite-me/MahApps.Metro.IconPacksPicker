using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;

namespace MahApps.Metro.IconPacksPicker
{
    partial class MainWindowModel: ObservableObject
    {
        private List<string> listKinds = new List<string>() { "" };
        private List<InstanceData> listIcons= new List<InstanceData>();
        private IEnumerable<InstanceData> filters;
        private readonly Dispatcher dispatcher;

        /// <summary>
        /// 
        /// </summary>
        public ICollectionView Icons
        {
            get { return icons; }
            set { SetProperty(ref icons, value); }
        }
        private ICollectionView icons;

        /// <summary>
        /// 
        /// </summary>
        public ICollectionView IconTypes
        {
            get { return iconTypes; }
            set { SetProperty(ref iconTypes, value); }
        }
        private ICollectionView iconTypes;
        /// <summary>
        /// 
        /// </summary>
        public int Page
        {
            get { return page; }
            set { SetProperty(ref page, value);
                fresh();
            }
        }
        private int page=1;
        /// <summary>
        /// 
        /// </summary>
        public int PageSize
        {
            get { return pageSize; }
            set { SetProperty(ref pageSize, value);
                fresh();
            }
        }
        private int pageSize=300;

        /// <summary>
        /// 
        /// </summary>
        public string SelectedType
        {
            get { return selectedType; }
            set { SetProperty(ref selectedType, value);
                fresh();
            }
        }
        private string selectedType;


        /// <summary>
        /// 
        /// </summary>
        public string SearchText
        {
            get { return searchText; }
            set { SetProperty(ref searchText, value);
                fresh();
            }
        }
        private string searchText;

        public MainWindowModel() {
            dispatcher= Dispatcher.CurrentDispatcher;
            Icons = new ListCollectionView(this.listIcons);
            IconTypes = new ListCollectionView(this.listKinds);
            Icons.GroupDescriptions.Add(new PropertyGroupDescription(nameof(InstanceData.TypeName)));
            Icons.Filter = filter;
           Task.Run(loading);
        }

        private bool filter(object obj)
        {
            if (filters == null) fresh();
            if (obj is InstanceData inst)
            {
                return filters!.Contains(inst);
            }
            return false;
        }

        private void fresh()
        {
            filters = listIcons;

            if (!string.IsNullOrWhiteSpace( selectedType ))
                filters = filters.Where(t => t.TypeName == selectedType);

            if (!string.IsNullOrWhiteSpace(searchText))
                filters = filters.Where(t => t.KindName!.Contains(searchText, StringComparison.OrdinalIgnoreCase));

            filters = filters.Skip(PageSize * (page - 1)).Take(PageSize).ToArray();
            dispatcher.Invoke(Icons.Refresh);
        }

        private void loading()
        {
            List<Assembly> assemblies = new();
                var assemblyFiles = Directory.EnumerateFiles(Path.GetFullPath( "./"), "*.dll");
            if (assemblyFiles != null)
            {
                foreach (var assemblyFile in assemblyFiles)
                {
                    try { assemblies.Add(Assembly.LoadFile(assemblyFile)); } catch { }
                }
            }

            assemblies=assemblies.Concat( new[] { Assembly.GetExecutingAssembly(), Assembly.GetEntryAssembly(), Assembly.GetCallingAssembly() })
                .Distinct().ToList()!;
            foreach (var one in assemblies)
            {
                try { tryLoading(one!); } catch { }
            }
        }

        private void tryLoading(Assembly assembly)
        {
            var allTypes =assembly.GetTypes();
            foreach (var type in allTypes.Where(t => t.IsClass && t.IsSubclassOf(typeof(PackIconControlBase))))
            {
                if (listKinds.Contains(type.Name)) continue;
                var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.SetProperty)
                    .Where(t => t.DeclaringType == type && t.PropertyType.IsEnum);
                if (!properties.Any()) continue;
                var propertyKind = properties.FirstOrDefault(t => t.Name == "Kind");
                if (propertyKind == null)
                {
                    propertyKind = properties.First();
                }

                var variable = Expression.Variable(type);
                var param = Expression.Parameter(propertyKind.PropertyType);
                var assign = Expression.Assign(variable, Expression.New(type));
                var setKind = Expression.Call(variable, propertyKind.SetMethod!, param);
                var @delegate = Expression.Lambda(
                    Expression.Block(
                        new[] { variable },
                        assign, setKind, variable),
                    param).Compile();

                foreach (var iconType in Enum.GetValues(propertyKind.PropertyType))
                {
                    listIcons.Add(new InstanceData(type, iconType, @delegate, dispatcher));
                }
                listKinds.Add(type.Name);

                fresh();
                //dispatcher.Invoke(IconTypes.Refresh);
                Task.Delay(100).Wait();
            }
        }

        [RelayCommand]
        private void Copy(InstanceData data)
        {
            System.Windows.Clipboard.SetDataObject($"<mahIcon:{data.TypeName} Kind=\"{data.KindName}\"/>");
        }
    }

    internal class InstanceData
    {
        private Type type;
        private object iconType;
        private Delegate @delegate;
        private Dispatcher dispatcher;

        public InstanceData(Type type, object iconType, Delegate @delegate, Dispatcher dispatcher)
        {
            this.type = type;
            this.iconType = iconType;
            this.@delegate = @delegate;
            this.dispatcher = dispatcher;
        }

        public PackIconControlBase Instance => instance ?? get();
        private PackIconControlBase instance;
        public string TypeName => type.Name;
        public string? KindName => iconType?.ToString();
        private PackIconControlBase get()
        {
            if (@delegate.DynamicInvoke(iconType) is PackIconControlBase packIcon)
            {
                packIcon.FontSize =
                packIcon.Width =
                packIcon.Height = 30;
                instance = packIcon;
            }
            return instance;
        }
    }

    internal class NotifyObject : INotifyPropertyChanged
    {

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Compares the current and new values for a given property. If the value has changed,
        /// raises the <see cref="PropertyChanging"/> event, updates the property with the new
        /// value, then raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <typeparam name="T">The type of the property that changed.</typeparam>
        /// <param name="field">The field storing the property's value.</param>
        /// <param name="newValue">The property's value after the change occurred.</param>
        /// <param name="propertyName">(optional) The name of the property that changed.</param>
        /// <returns><see langword="true"/> if the property was changed, <see langword="false"/> otherwise.</returns>
        /// <remarks>
        /// The <see cref="PropertyChanging"/> and <see cref="PropertyChanged"/> events are not raised
        /// if the current and new value for the target property are the same.
        /// </remarks>
        protected bool SetProperty<T>([NotNullIfNotNull("newValue")] ref T field, T newValue, [CallerMemberName] string? propertyName = null)
        {
            // We duplicate the code here instead of calling the overload because we can't
            // guarantee that the invoked SetProperty<T> will be inlined, and we need the JIT
            // to be able to see the full EqualityComparer<T>.Default.Equals call, so that
            // it'll use the intrinsics version of it and just replace the whole invocation
            // with a direct comparison when possible (eg. for primitive numeric types).
            // This is the fastest SetProperty<T> overload so we particularly care about
            // the codegen quality here, and the code is small and simple enough so that
            // duplicating it still doesn't make the whole class harder to maintain.
            if (EqualityComparer<T>.Default.Equals(field, newValue))
            {
                return false;
            }

           // OnPropertyChanging(propertyName);

            field = newValue;

            PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(propertyName));

            return true;
        }
    }
}
