﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using static LiteDB.Constants;

namespace LiteDB.Engine
{
    /// <summary>
    /// Memory file reader - must call Dipose after use to return reader into pool
    /// 1 instance per thread - (NO thread safe)
    /// </summary>
    internal class DiskReader : IDisposable
    {
        private readonly MemoryCache _cache;
        private readonly AesEncryption _aes;

        private readonly StreamPool _dataPool;
        private readonly StreamPool _logPool;

        private readonly Lazy<Stream> _dataStream;
        private readonly Lazy<Stream> _logStream;

        public DiskReader(MemoryCache cache, StreamPool dataPool, StreamPool logPool, AesEncryption aes)
        {
            _cache = cache;
            _dataPool = dataPool;
            _logPool = logPool;
            _aes = aes;

            _dataStream = new Lazy<Stream>(() => _dataPool.Rent());
            _logStream = new Lazy<Stream>(() => _logPool.Rent());
        }

        public PageBuffer ReadPage(long position, bool writable, PageMode mode)
        {
            var stream = mode == PageMode.Data ?
                _dataStream.Value :
                _logStream.Value;

            var page = writable ?
                _cache.GetWritablePage(position, mode, (pos, buf) => this.ReadStream(stream, pos, buf)) :
                _cache.GetReadablePage(position, mode, (pos, buf) => this.ReadStream(stream, pos, buf));

            return page;
        }

        /// <summary>
        /// Read bytes from stream into buffer slice
        /// </summary>
        private void ReadStream(Stream stream, long position, BufferSlice buffer)
        {
            stream.Position = position;

            // read encrypted or plain data from Stream into buffer
            if (_aes != null)
            {
                _aes.Decrypt(stream, buffer);
            }
            else
            {
                stream.Read(buffer.Array, buffer.Offset, buffer.Count);
            }
        }

        /// <summary>
        /// Request for a empty, writable non-linked page (same as DiskService.NewPage)
        /// </summary>
        public PageBuffer NewPage()
        {
            return _cache.NewPage();
        }

        /// <summary>
        /// When dispose, return stream to pool
        /// </summary>
        public void Dispose()
        {
            if (_dataStream.IsValueCreated)
            {
                _dataPool.Return(_dataStream.Value);
            }

            if (_logStream.IsValueCreated)
            {
                _logPool.Return(_logStream.Value);
            }
        }
    }
}