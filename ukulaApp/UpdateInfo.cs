using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using Windows.System;

namespace ukulaApp
{
    public class UpdateInfo
    {
        public string Version { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string Changelog { get; set; } = "";
        public string ChangelogUrl { get; set; } = "";
    }

}
