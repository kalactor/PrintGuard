namespace PrintGuardWinUI.ViewModels;

public sealed class PrinterSelectionItemViewModel : ViewModelBase
{
    private bool _isSelected;

    public PrinterSelectionItemViewModel(string name, bool isSelected)
    {
        Name = name;
        _isSelected = isSelected;
    }

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
