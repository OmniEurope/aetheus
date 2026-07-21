// SPDX-License-Identifier: EUPL-1.2
namespace Aetheus.Back.Data.Entities;

public class DockerImage
{
    public int Id { get; set; }
    public int ServerId { get; set; }
    public string ImageId { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public DateTime Created { get; set; }
    public DateTime LastUpdated { get; set; }

    // Navigation
    public Server Server { get; set; } = null!;
}
