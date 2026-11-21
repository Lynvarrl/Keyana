namespace Keyana.Storage;

/// <summary>
/// 一分钟的绝对快照。
///
/// <paramref name="Counts"/> 是「次数」向量（按键、鼠标按键、滚轮手势动作）；
/// 滚轮距离单独两列存放，**不进** 计数向量——它和次数不可通约，混进
/// <see cref="TotalCount"/> 会重新制造「滚轮一滑几百格」的量级失衡。
/// </summary>
public sealed record MinuteBucketSnapshot(
    long BucketStartUtc,
    uint[] Counts,
    long WheelDistanceUp = 0,
    long WheelDistanceDown = 0)
{
    public long TotalCount => Counts.Aggregate<uint, long>(0, (total, count) => total + count);

    /// <summary>上下方向的距离合计（原始 delta，一格 = 120）。</summary>
    public long WheelDistance => WheelDistanceUp + WheelDistanceDown;

    public MinuteBucketSnapshot Copy() =>
        new(BucketStartUtc, (uint[])Counts.Clone(), WheelDistanceUp, WheelDistanceDown);
}
