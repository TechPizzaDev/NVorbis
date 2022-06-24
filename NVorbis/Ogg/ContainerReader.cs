﻿using NVorbis.Contracts;
using NVorbis.Contracts.Ogg;
using System;
using System.Collections.Generic;
using System.IO;

namespace NVorbis.Ogg
{
    /// <summary>
    /// Implements <see cref="IContainerReader"/> for Ogg format files for low memory cost.
    /// </summary>
    public sealed class ContainerReader : IContainerReader
    {
        private IPageReader _reader;
        private List<WeakReference<IPacketProvider>> _packetProviders;
        private bool _foundStream;

        /// <summary>
        /// Gets or sets the callback to invoke when a new stream is encountered in the container.
        /// </summary>
        public NewStreamHandler NewStreamCallback { get; set; }

        /// <summary>
        /// Returns a list of streams available from this container.
        /// </summary>
        public IReadOnlyList<IPacketProvider> GetStreams()
        {
            List<IPacketProvider> list = new List<IPacketProvider>(_packetProviders.Count);
            for (int i = 0; i < _packetProviders.Count; i++)
            {
                if (_packetProviders[i].TryGetTarget(out IPacketProvider pp))
                {
                    list.Add(pp);
                }
                else
                {
                    list.RemoveAt(i);
                    --i;
                }
            }
            return list;
        }
        /// <summary>
        /// Gets whether the underlying stream can seek.
        /// </summary>
        public bool CanSeek { get; }

        /// <inheritdoc/>
        public long WasteBits => _reader.WasteBits;

        /// <inheritdoc/>
        public long ContainerBits => _reader.ContainerBits;

        /// <summary>
        /// Creates a new instance of <see cref="ContainerReader"/>.
        /// </summary>
        /// <param name="stream">The <see cref="Stream"/> to read.</param>
        /// <param name="leaveOpen"><see langword="false"/> to close the stream when disposed, otherwise <see langword="true"/>.</param>
        public ContainerReader(Stream stream, bool leaveOpen)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            _packetProviders = new List<WeakReference<IPacketProvider>>();

            if (stream.CanSeek)
            {
                _reader = new PageReader(stream, leaveOpen, ProcessNewStream);
                CanSeek = true;
            }
            else
            {
                _reader = new ForwardOnlyPageReader(stream, leaveOpen, ProcessNewStream);
            }
        }

        /// <summary>
        /// Attempts to initialize the container.
        /// </summary>
        /// <returns><see langword="true"/> if successful, otherwise <see langword="false"/>.</returns>
        public bool TryInit()
        {
            return FindNextStream();
        }

        /// <summary>
        /// Finds the next new stream in the container.
        /// </summary>
        /// <returns><c>True</c> if a new stream was found, otherwise <c>False</c>.</returns>
        public bool FindNextStream()
        {
            _reader.Lock();
            try
            {
                _foundStream = false;
                while (_reader.ReadNextPage())
                {
                    if (_foundStream)
                    {
                        return true;
                    }
                }
                return false;
            }
            finally
            {
                _reader.Release();
            }
        }

        private bool ProcessNewStream(IPacketProvider packetProvider)
        {
            bool relock = _reader.Release();
            try
            {
                if (NewStreamCallback?.Invoke(packetProvider) ?? true)
                {
                    _packetProviders.Add(new WeakReference<IPacketProvider>(packetProvider));
                    _foundStream = true;
                    return true;
                }
                return false;
            }
            finally
            {
                if (relock)
                {
                    _reader.Lock();
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _reader?.Dispose();
            _reader = null;
        }
    }
}
