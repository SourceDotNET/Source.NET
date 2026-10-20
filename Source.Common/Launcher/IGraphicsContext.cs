namespace Source.Common.Launcher;

public interface IGraphicsContext {
	nint HardwareHandle { get; }

	void MakeCurrent();
	void ReleaseCurrent();
	void SetSwapInterval(float swapInterval);
	void SwapBuffers();
}
