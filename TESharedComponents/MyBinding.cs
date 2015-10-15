﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Collections;

namespace TikzEdt
{
    public class MyBinding<T> where T : class, INotifyPropertyChanged
    {
        protected Action<T> Fire;
        protected Action Fail;
        protected string Key;

        protected T _Source;
        public virtual T Source
        {
            get { return _Source; }
            set 
            {
                if (_Source != value)
                {
                    if (_Source != null) 
                        UnRegisterHandler();
                    _Source = value;
                    if (_Source != null)
                        RegisterHandler();
                    OnSourceChanged();
                }

            }
        }

        protected virtual void OnSourceChanged()
        {
            Execute();
        }

        protected virtual void UnRegisterHandler()
        {
            Source.PropertyChanged -= new PropertyChangedEventHandler(o_PropertyChanged);
        }

        protected virtual void RegisterHandler()
        {
            Source.PropertyChanged += new PropertyChangedEventHandler(o_PropertyChanged);
        }

        void o_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == Key)
                Execute();
        }

        public void Trigger(string PropertyName)
        {
 	        if (PropertyName == Key)
                Execute();
        }

        protected void Execute()
        {
            if (Fire == null) 
                return;
            if (Source == null)
                ExecuteFail();
            try
            {
                Fire(Source);
            } 
            catch (Exception)
            {
                ExecuteFail();
            }
        }

        public void ExecuteFail()
        {
            if (Fail == null) 
                return;
            try
            {
                Fail();
            } 
            catch (Exception)
            {
                // do nothing
            }
        }

        public MyBinding(string Key, Action<T> Fire, Action Fail)
        {
            this.Key = Key;
            this.Fire = Fire;
            this.Fail = Fail;
        }

    }

    public class MyCollectionBinding<T> : MyBinding<T> where T : class, INotifyPropertyChanged, INotifyCollectionChanged
    {
        NotifyCollectionChangedEventHandler Handler = null;
        public MyCollectionBinding(NotifyCollectionChangedEventHandler Handler)
            : base("", null, null)
        {
            this.Handler = Handler;
        }
        protected override void  RegisterHandler()
        {
            Source.CollectionChanged += new NotifyCollectionChangedEventHandler(Source_CollectionChanged);
        }
        protected override void UnRegisterHandler()
        {
            Source.CollectionChanged -= new NotifyCollectionChangedEventHandler(Source_CollectionChanged);
        }

        protected override void  OnSourceChanged()
        {
 	         Source_CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
             //Source_CollectionChanged(Source, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, Source));
        }

        protected virtual void Source_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (Handler != null)
                Handler(sender, e);
        }
    }

    /// <summary>
    /// T is the type of the binding source.
    /// S is the type of the items in the binding source.
    /// U is the type of the items in the target list
    /// 
    /// TODO: when source changes, the list currently is cleared, but the elements of the new source are not automatically added.
    /// </summary>
    /// <typeparam name="T">the type of the binding source.</typeparam>
    /// <typeparam name="S">the type of the items in the binding source.</typeparam>
    /// <typeparam name="U">the type of the items in the target list.</typeparam>
    public class MyIListBinding<T, S, U> : MyCollectionBinding<T> 
        where T : class, INotifyPropertyChanged, INotifyCollectionChanged, IEnumerable<S>
    {
        IList Target = null;
        Func<S, U> Creator = null;
        Func<U, S> Query = null;

        public MyIListBinding(IList Target, Func<S, U> Creator, Func<U, S> Query)
            : base(null)
        {
            this.Target = Target;
            this.Creator = Creator;
            this.Query = Query;
        }

        protected override void Source_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (Target == null || Creator == null)
                return;

            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    var ToInsert = e.NewItems.OfType<S>().Select(x => Creator(x));
                    if (e.NewStartingIndex >= 0)
                        Target.InsertRange(e.NewStartingIndex, ToInsert);
                    else
                        Target.AddRange(ToInsert);
                    break;

                case NotifyCollectionChangedAction.Move:
                case NotifyCollectionChangedAction.Replace:
                    Source_CollectionChanged(sender, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, e.OldItems));
                    Source_CollectionChanged(sender, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, e.NewItems, e.NewStartingIndex));
                    break;

                case NotifyCollectionChangedAction.Remove:
                    var ToRemove = Target.OfType<U>().Where( u => e.OldItems.Contains( Query(u) ) );
                    Target.RemoveRange(ToRemove);
                    break;

                case NotifyCollectionChangedAction.Reset:
                    Target.Clear();
                    break;
            }
        }

        protected override void OnSourceChanged()
        {
            Source_CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            if (Source != null && Source.Count() > 0)
                Source_CollectionChanged(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, Source.ToList()));
        }
    }

    public class SourceProvider<T,U> : MyBinding<T> 
        where T : class,INotifyPropertyChanged 
        where U : class, INotifyPropertyChanged
    {
        List<MyBinding<U>> Children = new List<MyBinding<U>>();
        Func<T, U> Provider;
      
        public void Add(MyBinding<U> b)
        {
            Children.Add(b);
            OnFire(Source, new MyBinding<U>[] { b });
        }

        void OnFire(T src) { OnFire(src, Children); }
        void OnFire(T src, IEnumerable<MyBinding<U>> ChildSet)
        {
            U NewSource;
            try
            {
                NewSource = Provider(src);
            }
            catch (Exception )
            {
                OnFail(ChildSet);
                return;
            }

            foreach (var c in ChildSet)
                c.Source = NewSource;

        }
        void OnFail() { OnFail(Children); }
        void OnFail(IEnumerable<MyBinding<U>> ChildSet)
        {
            foreach (var c in ChildSet)
                c.Source = null;
        }

        public SourceProvider(string Key, Func<T, U> Provider)
            : base(Key, null, null)
        {
            Fire = src => OnFire(src);
            Fail = () => OnFail();

            this.Provider = Provider;
        }
    }

    /// <summary>
    /// Usually bindings are added to the internal ist of all bindings.
    /// 
    /// ...unless the optional parameter is supplied...
    /// </summary>
    public static class BindingFactory
    {
        static List<object> AllBindings = new List<object>();

        public static MyBinding<T> CreateBinding<T>(T Source, string Key, Action<T> Fire, Action Fail, bool AddToGlobalList = true) 
            where T : class,INotifyPropertyChanged
        {
            var b = new MyBinding<T>(Key, Fire, Fail);
            b.Source = Source;

            if (AddToGlobalList)
                AllBindings.Add(b);

            return b;
        }

        public static MyBinding<T> CreateBindingSP<U, T>(SourceProvider<U, T> SrcProvider, string Key, Action<T> Fire, Action Fail, bool AddToGlobalList = true)
            where T : class,INotifyPropertyChanged
            where U : class,INotifyPropertyChanged
        {
            var b = new MyBinding<T>(Key, Fire, Fail);
            SrcProvider.Add(b);
            if (AddToGlobalList) 
                AllBindings.Add(b);
            return b;
        }

        public static SourceProvider<U, T> CreateProvider<U, T>(U Source, string Key, Func<U, T> Provider, bool AddToGlobalList = true)
            where T : class,INotifyPropertyChanged
            where U : class,INotifyPropertyChanged
        {
            var p = new SourceProvider<U, T>(Key, Provider);
            p.Source = Source;
            if (AddToGlobalList) 
                AllBindings.Add(p);
            return p;
        }

        public static SourceProvider<U, T> CreateProviderSP<V, U, T>(SourceProvider<V, U> SrcProvider, string Key, Func<U, T> Provider, bool AddToGlobalList = true)
            where T : class,INotifyPropertyChanged
            where U : class,INotifyPropertyChanged
            where V : class,INotifyPropertyChanged
        {
            var p = new SourceProvider<U, T>(Key, Provider);
            SrcProvider.Add(p);
            if (AddToGlobalList) 
                AllBindings.Add(p);
            return p;
        }

        public static MyCollectionBinding<T> CreateCollectionBindingSP<U, T>(SourceProvider<U, T> SrcProvider, NotifyCollectionChangedEventHandler Handler, bool AddToGlobalList = true)
            where T : class,INotifyPropertyChanged, INotifyCollectionChanged
            where U : class,INotifyPropertyChanged
        {
            var b = new MyCollectionBinding<T>(Handler);
            SrcProvider.Add(b);
            if (AddToGlobalList) 
                AllBindings.Add(b);
            return b;
        }

        public static MyIListBinding<SType, SItem, TItem> CreateIListBinding<SType, SItem, TItem>(SType Source, IList Target, Func<SItem, TItem> Creator, Func<TItem, SItem> Query, bool AddToGlobalList = true)
            where SType : class,INotifyPropertyChanged, INotifyCollectionChanged, IEnumerable<SItem>
        {
            var b = new MyIListBinding<SType, SItem, TItem>(Target, Creator, Query);
            b.Source = Source;

            if (AddToGlobalList)
                AllBindings.Add(b);

            return b;
        }
    }
}
