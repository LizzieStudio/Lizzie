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
        
        /// <summary>
        /// Returns table position under cursor location
        /// </summary>
        /// <param name="component"></param>
        Vector3 GetSpawnPos();

        void ResetView();

        void EnterSpawnMode(VisualComponentBase component);
        void ExitSpawnMode();
        
        Camera3D Camera { get; }
}
