﻿using System.Collections.Generic;
using System.Linq;
using Plainion.GraphViz.Model;
using Plainion.GraphViz.Presentation;

namespace Plainion.GraphViz.Algorithms
{
    /// <summary>
    /// Generates a "hide mask" containing all visible nodes NOT building cycles.
    /// </summary>
    public class ShowCycles : AbstractAlgorithm
    {
        public ShowCycles(IGraphPresentation presentation)
            : base(presentation)
        {
        }

        public INodeMask Compute()
        {
            var graph = Presentation.GetModule<ITransformationModule>().Graph;

            var mask = new NodeMask();
            mask.IsShowMask = false;
            mask.Label = "Cycles";

            mask.Set(FindCycles(graph));
            mask.Invert(Presentation);

            return mask;
        }

        private IEnumerable<Node> FindCycles(IGraph graph)
        {
            var unvisited = new HashSet<Node>(graph.Nodes.Where(Presentation.Picking.Pick));
            unvisited.RemoveWhere(n => n.In.Count == 0 || n.Out.Count == 0);

            while (unvisited.Count > 0)
            {
                var current = unvisited.First();
                unvisited.Remove(current);

                foreach (var node in FindCycles(unvisited, current, new HashSet<Node> { current }).SelectMany(x => x))
                {
                    yield return node;
                }
            }
        }

        private IEnumerable<IList<Node>> FindCycles(HashSet<Node> unvisited, Node current, HashSet<Node> visited)
        {
            foreach (var inNode in current.In.Select(edge => edge.Source).Where(Presentation.Picking.Pick))
            {
                if (visited.Contains(inNode))
                {
                    yield return new List<Node> { inNode, current };
                }
                else
                {
                    var visitedCopy = new HashSet<Node>(visited);
                    visitedCopy.Add(inNode);
                    unvisited.Remove(inNode);

                    foreach (var cycle in FindCycles(unvisited, inNode, visitedCopy))
                    {
                        if (cycle.First() != cycle.Last())
                        {
                            cycle.Add(current);
                        }

                        yield return cycle;
                    }
                }
            }
        }
    }
}
