namespace Mpls.Model
{
    using System;

    public class ResourceUsage
    {
        public ResourceUsage()
        {
        }

        public ResourceUsage(ResourceKind kind, int value)
        {
            if (value < 0)
            {
                throw new ArgumentException("Value should be 0 or more");
            }

            this.Kind = kind;
            this.Value = value;
        }

        public ResourceKind Kind { get; set; }

        public int Value { get; set; }

        public bool Requires(ResourceKind kind)
        {
            return kind == this.Kind && this.Value > 0;
        }
    }
}