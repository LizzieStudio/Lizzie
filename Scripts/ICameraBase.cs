using Godot;
using System;

public partial interface ICameraBase
{
        void StartDrag();
        void StopDrag();
        void ProcessDrag(Vector2 axis);

        void ProcessViewEvent(InputEvent @event);

        void ZoomIn();
        void ZoomOut();

        void ZoomComponent(VisualComponentBase component);
        
        void ResetView();

        Camera3D Camera { get; }
}
