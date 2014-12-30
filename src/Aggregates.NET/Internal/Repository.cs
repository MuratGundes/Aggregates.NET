﻿using Aggregates.Contracts;
using NEventStore;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.ObjectBuilder;
using NServiceBus.ObjectBuilder.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Internal
{
    // inspired / taken from NEventStore.CommonDomain
    // https://github.com/NEventStore/NEventStore/blob/master/src/NEventStore/CommonDomain/Persistence/EventStore/EventStoreRepository.cs

    public class Repository<T> : IRepository<T> where T : class, IEventSource
    {
        private static readonly ILog Logger = LogManager.GetLogger(typeof(Repository<>));
        private readonly IStoreEvents _store;
        private readonly IBuilder _builder;

        private readonly ConcurrentDictionary<String, ISnapshot> _snapshots = new ConcurrentDictionary<String, ISnapshot>();
        private readonly ConcurrentDictionary<String, IEventStream> _streams = new ConcurrentDictionary<String, IEventStream>();
        private Boolean _disposed;

        public Repository(IBuilder builder, IStoreEvents store)
        {
            _builder = builder;
            _store = store;
        }

        void IRepository.Commit(Guid commitId, IDictionary<String, String> headers)
        {
            foreach (var stream in _streams)
            {
                if (headers != null)
                    headers.ToList().ForEach(h => stream.Value.UncommittedHeaders[h.Key] = h.Value);
                try
                {
                    stream.Value.CommitChanges(commitId);
                }
                catch (ConcurrencyException e)
                {
                    // Send to aggregate ?
                    stream.Value.ClearChanges();
                    throw new ConflictingCommandException(e.Message, e);
                }
                catch (DuplicateCommitException)
                {
                    stream.Value.ClearChanges();
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed || !disposing)
                return;

            lock (_streams)
            {
                foreach (var stream in _streams)
                {
                    stream.Value.Dispose();
                }

                _snapshots.Clear();
                _streams.Clear();
            }
            _disposed = true;
        }


        public T Get<TId>(TId id)
        {
            return Get<TId>(Bucket.Default, id);
        }

        public T Get<TId>(TId id, Int32 version)
        {
            return Get<TId>(Bucket.Default, id, version);
        }

        public T Get<TId>(String bucketId, TId id)
        {
            return Get<TId>(bucketId, id, Int32.MaxValue);
        }

        public T Get<TId>(String bucketId, TId id, Int32 version)
        {
            Logger.DebugFormat("Retreiving aggregate id {0} version {1} from bucket {2} in store", id, version, bucketId);

            ISnapshot snapshot = GetSnapshot(bucketId, id, version);
            IEventStream stream = OpenStream(bucketId, id, version, snapshot);

            if (stream == null && snapshot == null) return (T)null;

            // Use a child container to provide the root with a singleton stream and possibly some other future stuff
            using (var builder = _builder.CreateChildBuilder())
            {
                // Call the 'private' constructor
                var root = Newup(stream, builder);

                if (snapshot != null && root is ISnapshottingEventSource)
                    ((ISnapshottingEventSource)root).RestoreSnapshot(snapshot);

                if (stream != null && (version == 0 || root.Version < version))
                {
                    // If they GET a currently open root, apply all the uncommitted events too
                    var events = stream.CommittedEvents.Concat(stream.UncommittedEvents);

                    root.Hydrate(events.Take(version - root.Version).Select(e => e.Body));

                }

                return root;
            }
        }

        public T New<TId>(TId id)
        {
            return New<TId>(Bucket.Default, id);
        }

        public T New<TId>(String bucketId, TId id)
        {
            // Use a child container to provide the root with a singleton stream and possibly some other future stuff
            using (var builder = _builder.CreateChildBuilder())
            {
                var stream = PrepareStream(bucketId, id);
                return Newup(stream, builder);
            }
        }

        private T Newup(IEventStream stream, IBuilder builder)
        {
            // Call the 'private' constructor
            var tCtor = typeof(T).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { }, null);
            var root = (T)tCtor.Invoke(null);

            // Todo: I bet there is a way to make a INeedBuilding<T> type interface
            //      and loop over each, calling builder.build for each T
            if (root is INeedStream)
                (root as INeedStream).Stream = stream;
            if (root is INeedBuilder)
                (root as INeedBuilder).Builder = builder;
            if (root is INeedEventFactory)
                (root as INeedEventFactory).EventFactory = builder.Build<IMessageCreator>();
            if (root is INeedRouteResolver)
                (root as INeedRouteResolver).Resolver = builder.Build<IRouteResolver>();

            return root;
        }


        private ISnapshot GetSnapshot<TId>(String bucketId, TId id, int version)
        {
            ISnapshot snapshot;
            var snapshotId = String.Format("{0}/{1}", bucketId, id);
            if (!_snapshots.TryGetValue(snapshotId, out snapshot))
            {
                _snapshots[snapshotId] = snapshot = _store.Advanced.GetSnapshot(bucketId, id.ToString(), version);
            }

            return snapshot;
        }

        private IEventStream OpenStream<TId>(String bucketId, TId id, int version, ISnapshot snapshot)
        {
            IEventStream stream;
            var streamId = String.Format("{0}/{1}", bucketId, id);
            if (_streams.TryGetValue(streamId, out stream))
                return stream;

            if (snapshot == null)
                return _streams[streamId] = _store.OpenStream(bucketId, id.ToString(), Int32.MinValue, version);
            else
                return _streams[streamId] = _store.OpenStream(snapshot, version);
        }

        private IEventStream PrepareStream<TId>(String bucketId, TId id)
        {
            IEventStream stream;
            var streamId = String.Format("{0}/{1}", bucketId, id);
            if (!_streams.TryGetValue(streamId, out stream))
                _streams[streamId] = stream = _store.CreateStream(bucketId, id.ToString());

            return stream;
        }
    }
}