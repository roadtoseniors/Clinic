namespace Clinic.Workspace;
public sealed record AttachmentInfo(int Id, string Name, long Size)
{
    public string SizeLabel => Size >= 1024 * 1024 ? $"{Size / 1024d / 1024d:0.##} МБ" : $"{Size / 1024d:0.#} КБ";
}

public sealed record AttachmentUpload(string Name, string Data);
