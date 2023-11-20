// // //------------------------------------------------------------------------------------------------- 
// // // <copyright file="QuestionList.cs" company="stephbu">
// // // Copyright (c) Steve Butler. All rights reserved.
// // // </copyright>
// // //-------------------------------------------------------------------------------------------------

namespace Dns
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    public class QuestionList : List<Question>
    {
        public int LoadFrom(byte[] bytes, int offset, ushort count)
        {
            var currentOffset = offset;

            for (var index = 0; index < count; index++)
            {
                // TODO: move this code into the Question object

                var question = new Question();

                question.Name = DnsProtocol.ReadString(bytes, ref currentOffset);

                question.Type = (ResourceType) (BitConverter.ToUInt16(bytes, currentOffset).SwapEndian());
                currentOffset += 2;

                question.Class = (ResourceClass) (BitConverter.ToUInt16(bytes, currentOffset).SwapEndian());
                currentOffset += 2;

                this.Add(question);
            }

            var bytesRead = currentOffset - offset;
            return bytesRead;
        }

        public long WriteToStream(Stream stream)
        {
            var start = stream.Length;
            foreach (var question in this)
            {
                question.WriteToStream(stream);
            }
            var end = stream.Length;
            return end - start;
        }
    }
}