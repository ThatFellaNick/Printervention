/*
  Printervention
  Immutable printer details and mutable progress for one batch installation item.
*/

namespace Printervention
{
    internal sealed class PrinterInstallItem
    {
        public string IpAddress { get; set; }
        public string Model { get; set; }
        public string Vendor { get; set; }
        public string QueueName { get; set; }
        public string DriverName { get; set; }
        public string Status { get; set; }
        public string Details { get; set; }
        public DriverRecommendation Recommendation { get; set; }
    }
}
