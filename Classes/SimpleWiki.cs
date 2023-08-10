using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EmbeddingsScaleTestWithSQLServer.Classes
{
    public class SimpleWiki
    {
        public string id { get; set; } = string.Empty;
        public string title { get; set; } = string.Empty;
        public List<string> paragraphs { get; set; } = new List<string>();
    }
}
