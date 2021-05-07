using System;
using System.Collections.Generic;
using Mpls.Model;

namespace Mpls.Env
{
    public sealed class MachineBuilder
    {
        private static readonly Random Random = new Random();

        private string tag;

        private readonly List<ResourceUsage> resources = new List<ResourceUsage>
        {
            new ResourceUsage { Kind = ResourceKind.Cpu, Value = 100 },
            new ResourceUsage { Kind = ResourceKind.Memory, Value = 100 }
        };

        public Machine[] BuildMany(int count)
        {
            Machine[] containers = new Machine[count];
            int seed = Random.Next();

            for (int i = 0; i < count; i++)
            {
                Machine machine = new Machine
                {
                    Name = "Machine" + seed + i,
                    Tag = this.tag,
                    AvailableResources = this.resources.ToArray().Copy()
                };

                containers[i] = machine;
            }

            return containers;
        }

        public MachineBuilder WithCpu(int value)
        {
            this.resources.AddOrSetResourceDefaults(ResourceKind.Cpu, value);
            return this;
        }

        public MachineBuilder WithMemory(int value)
        {
            this.resources.AddOrSetResourceDefaults(ResourceKind.Memory, value);
            return this;
        }

        public MachineBuilder Clean()
        {
            this.resources.Clear();
            return this;
        }

        public MachineBuilder WithTag(string value)
        {
            this.tag = value;
            return this;
        }
    }
    internal static class ResourceUsageExtensions
    {

        public static void AddOrSetResourceDefaults(this IList<ResourceUsage> resources, ResourceKind kind, int defaultValue)
        {
            int index = -1;
            for (int i = 0; i < resources.Count; i++)
            {
                if (resources[i].Kind == kind)
                {
                    index = i;
                    resources[i].Value = defaultValue;
                }
            }

            if (index < 0)
            {
                resources.Add(new ResourceUsage(kind, defaultValue));
            }
        }
    }
}

