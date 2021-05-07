namespace Mpls.SolverContract
{
    using System;
    using Mpls.Model;

    public class SolverParameters
    {
        public ContainerSpec[] ContainerSpecs { get; set; }

        public Machine[] Machines { get; set; }

        public Allocation[] CurrentAllocations { get; set; }

        public ResourceKind[] OptimizeResources { get; set; } = { ResourceKind.Cpu, ResourceKind.Memory };

        public ResourceKind? MaximizeResourceUsageFor { get; set; }

        public ResourceKind? HardConstraintResource { get; set; }

        public int InitialRandomGuessIterations { get; set; } = 0;

        public int BestWorstSwappingIterations { get; set; } = 1000;

        public int FullScanIterations { get; set; } = 1;

        public int FullScanSwapContainerIterations { get; set; } = 1;

        public TimeSpan? Timeout { get; set; }

        public decimal? AllowedChurnPercentage { get; set; }
    }
}