namespace Mpls.Model
{
    public class ContainerSpec
    {
        public ResourceUsage[] ResourceUsages { get; set; }

        public int DesiredInstanceCount { get; set; }

        public string Name { get; set; }

    }
}