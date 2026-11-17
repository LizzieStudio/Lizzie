namespace Lizzie;

public class Project
{
    public string Folder { get; set; }

    public string Name => Folder == null ? null : ProjectFolder.NameOf(Folder);
}
