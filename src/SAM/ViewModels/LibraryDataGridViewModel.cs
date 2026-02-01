using DevExpress.Mvvm.CodeGenerators;
using SAM.Extensions;

namespace SAM.ViewModels;

[GenerateViewModel]
public partial class LibraryDataGridViewModel : LibraryViewModel
{
    [GenerateProperty] private LibraryGridSettings _settings;

    public LibraryDataGridViewModel(LibraryGridSettings settings) : base(settings)
    {
        Settings = settings;

        Refresh().SafeFireAndForget(e => log.Error("Failed to refresh library", e));

        _loading = false;
    }
}
