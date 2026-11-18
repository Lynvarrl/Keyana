namespace Keyana.Storage;

/// <summary>
/// <see cref="StatisticsRecorder"/> 需要的存储能力。抽成接口是为了让保存队列的
/// 并发、失败注入与退出竞态可以脱离真实 SQLite 测试。
/// </summary>
public interface IMinuteBucketStore
{
    Task<MinuteBucketSnapshot?> GetMinuteBucketAsync(
        long bucketStartUtc,
        CancellationToken cancellationToken = default);

    Task UpsertMinuteBucketAsync(
        MinuteBucketSnapshot snapshot,
        CancellationToken cancellationToken = default);
}
