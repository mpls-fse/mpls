
namespace Mpls.Model
{
    using System;
    using System.Text;

    public class AllocationMetric
    {
        public int Index { get; set; }

        public string MachineName { get; set; }

        public ResourceKind Resource { get; set; }

        public int Allocated { get; set; }

        public int TotalAvailable { get; set; }

        public int Difference => this.TotalAvailable - this.Allocated;
    }
}