using LinuxInstaller.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LinuxInstaller.Services;

public interface IStorageManager
{
    Task<IReadOnlyList<Disk>> GetDisksAsync(CancellationToken cancellationToken = default);
}
