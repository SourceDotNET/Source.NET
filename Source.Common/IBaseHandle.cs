namespace Source.Common;

public interface IBaseHandle
{
	uint Unpack();

	bool Equals(BaseHandle otherHandle);
	bool Equals(object? obj);
	int GetEntryIndex();
	int GetHashCode();
	int GetSerialNumber();
	bool IsValid();
}
