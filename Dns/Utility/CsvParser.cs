// //------------------------------------------------------------------------------------------------- 
// // <copyright file="CsvParser.cs" company="stephbu">
// // Copyright (c) Steve Butler. All rights reserved.
// // </copyright>
// //-------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;

namespace Dns.Utility
{
    /// <summary>Parses CSV files</summary>
    public class CsvParser
    {
        private static readonly char[] CSVDELIMITER = {','};
        private static readonly char[] COLONDELIMITER = {':'};

        private readonly string _filePath;

        private string _currentLine;
        private string[] _fields;

        private CsvParser()
        {
        }

        private CsvParser(string filePath) => _filePath = filePath;

        /// <summary>List of fields detected in CSV file</summary>
        public IEnumerable<string> Fields => _fields;

        /// <summary>
        ///   Returns enumerable collection of rows
        /// </summary>
        public IEnumerable<CsvRow> Rows
        {
            get
            {
                using FileStream stream = new(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using StreamReader csvReader = new(stream);
                while (true)
                {
                    if (csvReader.Peek() < 0)
                        yield break;

                    _currentLine = csvReader.ReadLine();
                    if (_currentLine == null)
                        yield break;
                    if(_currentLine.Trim() == string.Empty)
                        continue;
                    if ("#;".Contains(_currentLine[0]))
                    {
                        // is a comment
                        if (_currentLine.Length <= 1 || !_currentLine[1..].StartsWith("Fields")) continue;
                        string[] fieldDeclaration = _currentLine.Split(COLONDELIMITER);
                        _fields = fieldDeclaration.Length != 2 ? null : fieldDeclaration[1].Trim().Split(CSVDELIMITER);
                    }
                    else
                        yield return new CsvRow(_fields, _currentLine.Split(CSVDELIMITER));
                }
            }
        }

        /// <summary>
        ///   Create instance of CSV Parser
        /// </summary>
        /// <param name="filePath"> Path of file to parse </param>
        /// <returns> CSV Parser instance </returns>
        public static CsvParser Create(string filePath)
        {
            if (filePath == null)
                throw new ArgumentNullException("filePath");

            if (!File.Exists(filePath))
                throw new FileNotFoundException("File Not Found", filePath);

            CsvParser result = new CsvParser(filePath);
            return result;
        }
    }
}