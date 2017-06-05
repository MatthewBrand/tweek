﻿using Engine.Drivers.Context;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Engine.DataTypes;
using Couchbase;
using Couchbase.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Dynamic;
using FSharpUtils.Newtonsoft;

namespace Tweek.Drivers.CouchbaseDriver
{
    public class CouchBaseDriver : IContextDriver
    {
        readonly string _bucketName;
        private Func<string, IBucket> _getBucket;

        public CouchBaseDriver(Func<string, IBucket> getBucket,  string bucketName)
        {
            _bucketName = bucketName;
            _getBucket = getBucket;
        }

        public string GetKey(Identity identity) => "identity_" + identity.Type + "_" + identity.Id;

        public IBucket GetOrOpenBucket()
        {
            return _getBucket(_bucketName);
        }

        public async Task RemoveFromContext(Identity identity, string key)
        {
            var keyIdentity = GetKey(identity);
            var bucket = GetOrOpenBucket();
            var mutator = bucket.MutateIn<dynamic>(keyIdentity);
            await mutator.Remove(key).ExecuteAsync();
        }

        public async Task AppendContext(Identity identity, Dictionary<string, JsonValue> context)
        {
            var key = GetKey(identity);
            var bucket = GetOrOpenBucket();
            var mutator = context.Aggregate(bucket.MutateIn<dynamic>(key), (acc, next)=> acc.Upsert(next.Key, next.Value));
            await mutator.ExecuteAsync();
        }

        public async Task<Dictionary<string, JsonValue>> GetContext(Identity identity)
        {
            var key = GetKey(identity);
            var data = await GetFromAllSources<Dictionary<string, JsonValue>>(key);
            if (data == null)
            {
                return new Dictionary<string, JsonValue>();
            }
            return data;
        }

        private async Task<T> GetFromAllSources<T>(string key) where T:class
        {
            var bucket = GetOrOpenBucket();
            var document = await bucket.GetAsync<T>(key);
            if (document.Success) return document.Value;
            if (document.Status == global::Couchbase.IO.ResponseStatus.KeyNotFound) return null;
            var replica = (await bucket.GetFromReplicaAsync<T>(key));
            if (replica.Success) return replica.Value;
            if (replica.Status == global::Couchbase.IO.ResponseStatus.KeyNotFound) return null;
            throw new AggregateException(document.Exception ?? new Exception(document.Message),
                                          replica.Exception ?? new Exception(replica.Message));
        }

        public async Task RemoveIdentityContext(Identity identity)
        {
            await GetOrOpenBucket().RemoveAsync(GetKey(identity));
        }


    }
}
