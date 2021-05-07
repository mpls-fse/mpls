using System;
using System.Collections.Generic;
using Mpls.Model;

namespace Mpls.Env
{
    public sealed class ContainerBuilder
    {
        private static readonly Random Random = new Random();

        private readonly List<ResourceUsage> resources = new List<ResourceUsage>()
        {
            new ResourceUsage { Kind = ResourceKind.Cpu, Value = 5 },
            new ResourceUsage { Kind = ResourceKind.Memory, Value = 10 },
        };

        private int desiredInstanceCount = 8;

        public List<ResourceUsage> Resources => resources;

        public ContainerSpec[] BuildMany(int count)
        {
            ContainerSpec[] containers = new ContainerSpec[count];
            int seed = Random.Next();

            for (int i = 0; i < count; i++)
            {
                ContainerSpec container = new ContainerSpec
                {
                    Name = "Container" + seed + i,
                    ResourceUsages = this.Resources.ToArray().Copy(),
                    DesiredInstanceCount = this.desiredInstanceCount
                };

                containers[i] = container;
            }

            return containers;
        }

        public ContainerBuilder WithCpu(int value)
        {
            this.Resources.AddOrSetResourceDefaults(ResourceKind.Cpu, value);
            return this;
        }

        public ContainerBuilder WithMemory(int value)
        {
            this.Resources.AddOrSetResourceDefaults(ResourceKind.Memory, value);
            return this;
        }
        
        public ContainerBuilder WithInstanceCount(int value)
        {
            this.desiredInstanceCount = value;
            return this;
        }

        public ContainerBuilder Clean()
        {
            this.Resources.Clear();
            return this;
        }
    }
}

