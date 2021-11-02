﻿using System;

namespace NVorbis.Contracts
{
    struct HuffmanListNode : IComparable<HuffmanListNode>
    {
        public int Value;
        public int Length;
        public int Bits;
        public int Mask;

        public int CompareTo(HuffmanListNode y)
        {
            var len = Length - y.Length;
            if (len == 0)
            {
                return Bits - y.Bits;
            }
            return len;
        }
    }
}
