namespace Mpls.Model
{
    using System.Collections.Generic;

    internal class AllocationPlan
    {
        public AllocationPlan(IList<Allocation> allocations, IList<Allocation> newAllocations, IList<Allocation> deallocations)
        {
            this.Allocations = allocations;
            this.NewAllocations = newAllocations;
            this.Deallocations = deallocations;
        }

        public IList<Allocation> Allocations { get; }

        public IList<Allocation> NewAllocations { get; }

        public IList<Allocation> Deallocations { get; }
    }
}