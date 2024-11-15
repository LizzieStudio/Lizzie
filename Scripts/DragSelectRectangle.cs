using Godot;
using System;

public partial class DragSelectRectangle : Control
{
	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (_dragSelectInProcess)
		{
			QueueRedraw();
		}
	}

	public override void _Draw()
	{
		if (_dragSelectInProcess)
		{
			CurRectangle = new Rect2(_dragSelectOrigin,
				GetGlobalMousePosition() - _dragSelectOrigin);
			
			DrawRect(CurRectangle, new Color(1,1,0), false, 2);
		}
	}

	private bool _dragSelectInProcess;
	private Vector2 _dragSelectOrigin;
	
	public Rect2 CurRectangle { get; private set; }

	public void StartDragSelect()
	{
		_dragSelectOrigin = GetGlobalMousePosition();
		_dragSelectInProcess = true;
	}

	public Rect2 StopDragSelect()
	{
		_dragSelectInProcess = false;
		CurRectangle = new Rect2(0, 0, 0, 0);
		QueueRedraw();
		return new Rect2(_dragSelectOrigin, GetViewport().GetMousePosition());
	}
}
