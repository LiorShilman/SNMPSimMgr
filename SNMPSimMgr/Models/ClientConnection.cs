using CommunityToolkit.Mvvm.ComponentModel;

namespace SNMPSimMgr.Models;

public partial class ClientConnection : ObservableObject
{
    public string IpAddress { get; set; } = string.Empty;
    public bool IsInjection { get; set; }

    [ObservableProperty] private int _requestCount;
    [ObservableProperty] private string _lastOperation = string.Empty;
    [ObservableProperty] private string _lastSeen = string.Empty;
    [ObservableProperty] private string _targetDevice = string.Empty;
    [ObservableProperty] private bool _isActive;
}
