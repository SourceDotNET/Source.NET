namespace Source.Common.Launcher;

public interface IGraphicsContext {
	nint HardwareHandle { get; }

	void MakeCurrent();
	void SetSwapInterval(float swapInterval);
	void SwapBuffers();
}
