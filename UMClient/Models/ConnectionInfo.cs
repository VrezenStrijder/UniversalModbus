using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace UMClient.Models
{
    public partial class ConnectionInfo : ObservableObject
    {
        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private ConnectionMode mode;

        [ObservableProperty]
        private bool isConnected;

        [ObservableProperty]
        private string endpoint = string.Empty;

        [ObservableProperty]
        private bool isSelected;
    }


}
