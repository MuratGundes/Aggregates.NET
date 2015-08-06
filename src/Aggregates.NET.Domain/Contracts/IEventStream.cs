﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Contracts
{
    public interface IEventStream
    {
        String StreamId { get; }
        Int32 StreamVersion { get; }
        Int32 CommitVersion { get; }

        IEnumerable<IWritableEvent> Events { get; }

        void Add(Object @event, IDictionary<String, Object> headers);
        void Add(ISnapshot snapshot, IDictionary<String, Object> headers);
        void Commit(Guid commitId, IDictionary<String, Object> commitHeaders);

        void ClearChanges();
    }
}