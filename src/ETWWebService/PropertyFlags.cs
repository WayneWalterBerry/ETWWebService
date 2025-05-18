// <copyright file="PropertyFlags.cs" company="Wayne Walter Berry">
// Copyright (c) Wayne Walter Berry. All rights reserved.
// </copyright>

namespace ETWWebService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    [Flags]
    public enum PropertyFlags : uint
    {
        None = 0,
        Struct = 0x1,
        ParamLength = 0x2,
        ParamCount = 0x4,
        WBEMXMLFragment = 0x8,
        ParamFixedLength = 0x10,
        ParamFixedCount = 0x20,
        HasTags = 0x40,
        HasCustomSchema = 0x80
    }
}
