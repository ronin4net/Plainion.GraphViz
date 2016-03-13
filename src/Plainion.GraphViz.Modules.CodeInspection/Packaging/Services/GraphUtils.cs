﻿using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Plainion.GraphViz.Modules.CodeInspection.Packaging.Services
{
    static class GraphUtils
    {
        public static Edge Edge( Type source, Type target )
        {
            return new Edge
            {
                Source = Node( source ),
                Target = Node( target )
            };
        }

        public static Type Node(Type type)
        {
            var nodeType = type;

            if (type.GetCustomAttribute(typeof(CompilerGeneratedAttribute), true) != null)
            {
                nodeType = type.DeclaringType;
            }

            if (nodeType == null)
            {
                // e.g. code generated from Xml like ResourceManager
                nodeType = type;
            }

            return nodeType;
        }
    }
}
