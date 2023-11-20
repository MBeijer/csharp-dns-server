// //------------------------------------------------------------------------------------------------- 
// // <copyright file="CsvRow.cs" company="stephbu">
// // Copyright (c) Steve Butler. All rights reserved.
// // </copyright>
// //-------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Dns.Utility
{
    /// <summary>Represents row in comma separated value file</summary>
    public class CsvRow
    {
        private readonly string[] _fieldValues;
        private readonly Dictionary<string, string> _fieldsByName = new Dictionary<string, string>();

        internal CsvRow(IReadOnlyList<string> fields, string[] fieldValues)
        {
            _fieldValues = fieldValues;
            if ((fields == null) || (fields.Count != fieldValues.Length)) return;
            for (var index = 0; index < fields.Count; index++)
                _fieldsByName[fields[index]] = fieldValues[index];
        }

        /// <summary>Returns value for specified field ordinal</summary>
        /// <param name="index">Specifed field ordinal</param>
        /// <returns>Value of field</returns>
        public string this[int index] => _fieldValues[index];

        /// <summary>Returns value for specified field name</summary>
        /// <param name="name">Specified field name</param>
        /// <returns>Value of field</returns>
        public string this[string name] => _fieldsByName.TryGetValue(name, out var fieldValue) ? fieldValue : null;
    }
}