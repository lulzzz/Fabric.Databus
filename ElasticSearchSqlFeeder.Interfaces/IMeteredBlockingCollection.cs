﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ElasticSearchSqlFeeder.Interfaces
{
    public interface IMeteredBlockingCollection
    {
        bool Any();
        int Count { get; }
        bool IsCompleted { get; }
        void CompleteAdding();
    }

    public interface IMeteredBlockingCollection<T> : IMeteredBlockingCollection
    {
        T Take();
        void Add(T item);
        bool TryTake(out T cacheItem);
        string Name { get; }
    }
}
