// <copyright file="UserDataColumn.cs" company="Wayne Walter Berry">
// Copyright (c) Wayne Walter Berry. All rights reserved.
// </copyright>


namespace ETWWebService
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// Represents a column in the UserData section.
    /// </summary>
    public class UserDataColumn
    {
        public string Name { get; set; }
        public Type DataType { get; set; }
        public int Length { get; set; }
        public bool IsArray { get; set; }
        public uint ArrayCount { get; set; }
        public Dictionary<string, string> EnumValues { get; set; }
        public UserDataColumnStatus Status { get; set; }

        public UserDataColumn()
        {
            EnumValues = new Dictionary<string, string>();
            Status = UserDataColumnStatus.Unknown;
        }
    }
}
