using Keyana.Core;

namespace Keyana.Storage;

/// <summary>
/// 按键筛选：只让选中的按键参与展示与统计。
///
/// 筛选是纯展示层的事——数据库里的计数一个都不动，只是每次渲染前把没选中的键清零。
/// 这样取消筛选就能立刻恢复全部数据，不需要重新查询历史。
/// </summary>
public static class KeyFilter
{
    /// <summary>
    /// 按筛选生成一份可展示的计数；未筛选时返回 null，让调用方继续用原数组
    /// （避免每两秒一次的刷新多分配一个 132 元素的数组）。
    /// </summary>
    public static ulong[]? Visible(IReadOnlyList<ulong> counts, IReadOnlySet<KeyId>? filter)
    {
        ArgumentNullException.ThrowIfNull(counts);

        // 空集合没有意义（等于什么都不统计），统一按「未筛选」处理。
        if (filter is null || filter.Count == 0)
        {
            return null;
        }

        var visible = new ulong[counts.Count];
        foreach (var key in filter)
        {
            var index = (int)key;
            if ((uint)index < (uint)counts.Count)
            {
                visible[index] = counts[index];
            }
        }

        return visible;
    }

    /// <summary>把筛选描述成一句能直接显示的话。</summary>
    public static string Describe(IReadOnlySet<KeyId>? filter) =>
        filter is null || filter.Count == 0 ? "全部按键" : $"已筛选 {filter.Count} 个键";
}
