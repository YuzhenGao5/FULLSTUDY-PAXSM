// 文件名：ITickRing.cs
public interface ITickRing
{
    int TickCount { get; }
    float GetAngleForIndex(int index);
}
