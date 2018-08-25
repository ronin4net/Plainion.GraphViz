﻿using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Plainion.GraphViz.Modules.CodeInspection.Actors
{
    class DocumentSerializer
    {
        internal byte[] Serialize<T>(T doc)
        {
            using (var stream = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(stream, doc);

                return stream.ToArray();
            }
        }

        internal void Serialize<T>(T doc, string file)
        {
            using (var stream = new FileStream(file, FileMode.OpenOrCreate, FileAccess.Write))
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(stream, doc);
            }
        }

        internal T Deserialize<T>(byte[] blob)
        {
            using (var stream = new MemoryStream(blob))
            {
                var formatter = new BinaryFormatter();
                return (T)formatter.Deserialize(stream);
            }
        }

        internal T Deserialize<T>(string file)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read))
            {
                var formatter = new BinaryFormatter();
                return (T)formatter.Deserialize(stream);
            }
        }
    }
}
