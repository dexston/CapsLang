using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Input;

namespace CapsLang.ViewModels
{
    public sealed class RuleSectionViewModel : ViewModelBase
    {
        private readonly Func<string> _titleGetter;
        private readonly Func<string> _descGetter;
        private readonly Func<string> _emptyHintGetter;
        private readonly Func<IEnumerable<string>, bool> _canCommit;

        private ObservableCollection<string> _items;
        public ReadOnlyObservableCollection<string> Items { get; private set; }

        public string Title => _titleGetter();
        public string Description => _descGetter();
        public string EmptyHint => _emptyHintGetter();

        public string CounterText => _items.Count.ToString();
        public bool IsEmpty => _items.Count == 0;

        public ICommand RemoveItemCommand { get; }

        public RuleSectionViewModel(
            IEnumerable<string> initialItems,
            Func<string> titleGetter,
            Func<string> descGetter,
            Func<string> emptyHintGetter,
            Func<IEnumerable<string>, bool> canCommit)
        {
            _titleGetter = titleGetter;
            _descGetter = descGetter;
            _emptyHintGetter = emptyHintGetter;
            _canCommit = canCommit;

            ResetItems(initialItems);

            RemoveItemCommand = new RelayCommand<string>(RemoveItem);
        }

        public bool TryAddItem(string normalizedValue)
        {
            if (string.IsNullOrWhiteSpace(normalizedValue))
                return false;

            foreach (string existing in _items)
            {
                if (string.Equals(existing, normalizedValue, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            var candidate = new List<string>(_items.Count + 1);
            candidate.Add(normalizedValue);
            candidate.AddRange(_items);

            if (_canCommit != null && !_canCommit(candidate))
                return false;

            _items.Insert(0, normalizedValue);
            return true;
        }

        private void RemoveItem(string item)
        {
            if (string.IsNullOrEmpty(item)) return;
            if (!_items.Contains(item)) return;

            var candidate = new List<string>(_items);
            candidate.Remove(item);

            if (_canCommit != null && !_canCommit(candidate))
                return;

            _items.Remove(item);
        }

        public void ResetItems(IEnumerable<string> newItems)
        {
            var filtered = new List<string>();
            if (newItems != null)
            {
                foreach (string item in newItems)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        filtered.Add(item);
                    }
                }
            }

            if (_items != null)
            {
                _items.CollectionChanged -= OnItemsCollectionChanged;
            }

            _items = new ObservableCollection<string>(filtered);
            _items.CollectionChanged += OnItemsCollectionChanged;
            Items = new ReadOnlyObservableCollection<string>(_items);

            OnPropertyChanged(nameof(Items));
            NotifyCollectionStateChanged();
        }

        public void RefreshLocalization()
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(EmptyHint));
        }

        private void OnItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            NotifyCollectionStateChanged();
        }

        private void NotifyCollectionStateChanged()
        {
            OnPropertyChanged(nameof(CounterText));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }
}

