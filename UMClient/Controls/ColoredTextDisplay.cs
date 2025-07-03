using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Media;
using System.Collections;
using System.Collections.Specialized;
using Avalonia.Animation.Easings;
using Avalonia.Animation;
using Avalonia.Styling;

namespace UMClient.Controls
{
    public class ColoredTextDisplay : TemplatedControl
    {
        public static readonly StyledProperty<IEnumerable?> ItemsProperty =
            AvaloniaProperty.Register<ColoredTextDisplay, IEnumerable?>(nameof(Items));

        public static readonly StyledProperty<bool> AutoScrollProperty =
            AvaloniaProperty.Register<ColoredTextDisplay, bool>(nameof(AutoScroll), true);

        private ScrollViewer? scrollViewer;
        private ItemsControl? itemsControl;
        private INotifyCollectionChanged? currentCollection;


        public IEnumerable? Items
        {
            get => GetValue(ItemsProperty);
            set => SetValue(ItemsProperty, value);
        }

        public bool AutoScroll
        {
            get => GetValue(AutoScrollProperty);
            set => SetValue(AutoScrollProperty, value);
        }

        static ColoredTextDisplay()
        {
            ItemsProperty.Changed.AddClassHandler<ColoredTextDisplay>((x, e) =>
            {
                x.OnItemsChanged(e);
            });
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);

            scrollViewer = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");
            itemsControl = e.NameScope.Find<ItemsControl>("PART_ItemsControl");

            if (itemsControl != null)
            {
                itemsControl.ItemsSource = Items;
            }

            SubscribeToCollectionChanged();
        }

        private void OnItemsChanged(AvaloniaPropertyChangedEventArgs e)
        {
            if (currentCollection != null)
            {
                currentCollection.CollectionChanged -= OnCollectionChanged;
            }

            if (itemsControl != null)
            {
                itemsControl.ItemsSource = e.NewValue as IEnumerable;
            }

            // 订阅新集合
            SubscribeToCollectionChanged();
        }

        private void SubscribeToCollectionChanged()
        {
            currentCollection = Items as INotifyCollectionChanged;
            if (currentCollection != null)
            {
                currentCollection.CollectionChanged += OnCollectionChanged;
            }
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 当集合发生变化时，自动滚动到底部
            if (AutoScroll && scrollViewer != null)
            {

                scrollViewer.Measure(Size.Infinity);
                scrollViewer.Arrange(new Rect(scrollViewer.DesiredSize));


                // 使用 Dispatcher 确保在 UI 更新后执行滚动
                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    await Task.Delay(10);

                    scrollViewer.ScrollToEnd();
                }, Avalonia.Threading.DispatcherPriority.Background);
            }
        }

        public void ScrollToEnd()
        {
            scrollViewer?.ScrollToEnd();
        }

        public void Clear()
        {
            if (Items is ObservableCollection<ColoredTextItem> collection)
            {
                collection.Clear();
            }
        }

    }

    public class ColoredTextItem
    {
        public string Text { get; set; } = string.Empty;
        public IBrush Foreground { get; set; } = Brushes.White;
    }

}
