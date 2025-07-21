using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SukiUI.Models;

namespace UMClient.Models
{
    public class ThemeOption
    {
        public string Display { get; set; } = string.Empty;

        public SukiColorTheme? Theme { get; set; }
    }



}
