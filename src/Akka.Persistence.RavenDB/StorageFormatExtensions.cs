using Akka.Persistence.RavenDB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Persistence.RavenDB
{
    public static class StorageFormatExtensions
    {
        private static readonly string _LeadingZerosFormat = new string('0', 19); // long

        public static string ToLeadingZerosFormat(this long number) => number.ToString(_LeadingZerosFormat);
    }
}
