﻿using NVorbis.Contracts;
using System;

namespace NVorbis
{
    class Mode
    {
        int _channels;
        bool _blockFlag;
        int _block0Size;
        int _block1Size;
        Mapping _mapping;

        public Mode(DataPacket packet, int channels, int block0Size, int block1Size, Mapping[] mappings)
        {
            _channels = channels;
            _block0Size = block0Size;
            _block1Size = block1Size;

            _blockFlag = packet.ReadBit();
            if (0 != packet.ReadBits(32))
            {
                throw new System.IO.InvalidDataException("Mode header had invalid window or transform type!");
            }

            int mappingIdx = (int)packet.ReadBits(8);
            if (mappingIdx >= mappings.Length)
            {
                throw new System.IO.InvalidDataException("Mode header had invalid mapping index!");
            }
            _mapping = mappings[mappingIdx];

            if (_blockFlag)
            {
                Windows = new float[][]
                {
                    new float[_block1Size],
                    new float[_block1Size],
                    new float[_block1Size],
                    new float[_block1Size],
                };
            }
            else
            {
                Windows = new float[][]
                {
                    new float[_block0Size],
                };
            }
            CalcWindows();
        }

        private void CalcWindows()
        {
            // 0: prev = s, next = s || BlockFlag = false
            // 1: prev = l, next = s
            // 2: prev = s, next = l
            // 3: prev = l, next = l

            for (int idx = 0; idx < Windows.Length; idx++)
            {
                float[] array = Windows[idx];

                int left = ((idx & 1) == 0 ? _block0Size : _block1Size) / 2;
                int wnd = BlockSize;
                int right = ((idx & 2) == 0 ? _block0Size : _block1Size) / 2;

                int leftbegin = wnd / 4 - left / 2;
                int rightbegin = wnd - wnd / 4 - right / 2;

                for (int i = 0; i < left; i++)
                {
                    double x = Math.Sin((i + .5) / left * Math.PI / 2);
                    x *= x;
                    array[leftbegin + i] = (float)Math.Sin(x * Math.PI / 2);
                }

                for (int i = leftbegin + left; i < rightbegin; i++)
                {
                    array[i] = 1.0f;
                }

                for (int i = 0; i < right; i++)
                {
                    double x = Math.Sin((right - i - .5) / right * Math.PI / 2);
                    x *= x;
                    array[rightbegin + i] = (float)Math.Sin(x * Math.PI / 2);
                }
            }
        }

        private bool GetPacketInfo(
            DataPacket packet,
            bool isLastInPage, 
            out int blockSize,
            out int windowIndex, 
            out int leftOverlapHalfSize,
            out int packetStartIndex,
            out int packetValidLength,
            out int packetTotalLength)
        {
            bool prevFlag, nextFlag;
            if (_blockFlag)
            {
                blockSize = _block1Size;
                prevFlag = packet.ReadBit();
                nextFlag = packet.ReadBit();
            }
            else
            {
                blockSize = _block0Size;
                prevFlag = nextFlag = false;
            }

            if (packet.IsShort)
            {
                windowIndex = 0;
                leftOverlapHalfSize = 0;
                packetStartIndex = 0;
                packetValidLength = 0;
                packetTotalLength = 0;
                return false;
            }

            int rightOverlapHalfSize = (nextFlag ? _block1Size : _block0Size) / 4;

            windowIndex = (prevFlag ? 1 : 0) + (nextFlag ? 2 : 0);
            leftOverlapHalfSize = (prevFlag ? _block1Size : _block0Size) / 4;
            packetStartIndex = blockSize / 4 - leftOverlapHalfSize;
            packetTotalLength = blockSize / 4 * 3 + rightOverlapHalfSize;
            packetValidLength = packetTotalLength - rightOverlapHalfSize * 2;

            if (isLastInPage && _blockFlag && !nextFlag)
            {
                // this fixes a bug in certain libvorbis versions where a long->short that crosses
                // a page boundary doesn't get counted correctly in the first page's granulePos
                packetValidLength -= _block1Size / 4 - _block0Size / 4;
            }
            return true;
        }

        public bool Decode(
            DataPacket packet, 
            float[][] buffer, 
            out int packetStartindex,
            out int packetValidLength,
            out int packetTotalLength)
        {
            if (GetPacketInfo(
                packet, 
                isLastInPage: false, 
                out int blockSize,
                out int windowIndex, 
                out _,
                out packetStartindex,
                out packetValidLength,
                out packetTotalLength))
            {
                _mapping.DecodePacket(packet, blockSize, _channels, buffer);

                nint channels = _channels;
                Span<float> window = Windows[windowIndex].AsSpan(0, blockSize);
                for (int i = 0; i < window.Length; i++)
                {
                    float v = window[i];
                    for (nint ch = 0; ch < channels; ch++)
                    {
                        buffer[ch][i] *= v;
                    }
                }
                return true;
            }
            return false;
        }

        public int GetPacketSampleCount(DataPacket packet, bool isLastInPage)
        {
            GetPacketInfo(packet, isLastInPage, out _, out _, out _, out int packetStartIndex, out int packetValidLength, out _);
            return packetValidLength - packetStartIndex;
        }

        public int BlockSize => _blockFlag ? _block1Size : _block0Size;

        public float[][] Windows { get; private set; }
    }
}
