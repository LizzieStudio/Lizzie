# Simple Free-Look Camera
A C# implementation of the SimpleFreeLookCamera found in the AssetStore:

## Original description:
A simple 3D 100-line free-look camera for Godot 4.0 that mimics the functionality of the editor's camera for in-game use.

Ported from [Richardhyy's Godot 4.0 version](https://github.com/richardhyy/simple-free-look-camera-4),
who ported from [Adamviola's Godot 3.2 version](https://github.com/adamviola/simple-free-look-camera).

## Why should you use the C# version?
- If you need to interact with the camera from your C# code you'll need the C# version of it. Currently, there is no easy way to perform this:
[Referencing a custom GDScript class from C# - Can it be done?!?!](https://www.reddit.com/r/godot/comments/12um6jr/referencing_a_custom_gdscript_class_from_c_can_it/)
[Cross-language scripting](https://docs.godotengine.org/en/stable/tutorials/scripting/cross_language_scripting.html#accessing-fields)
- If you want your project to be fully written in C# and you need a free camera. 
- [You, the reader, place any other reason here]

## Installation
Attach `FreeLookCameraBase.cs` or `FreeLookCamera.cs` to your Camera3D node. That's it!

- Attaching FreeLookCameraBase behaves exactly like the original GD script version.

- If you attach FreeLookCamera.cs instead, you'll be able to switch from your current game camera's to the free one, just setting the **Enabled** property of it. For instance by handling an input action from your script:

```c++
...
if (Input.IsActionJustReleased("ToggleFreeCameraAction"))
{
    _freeCameraNode.Enabled = !_freeCameraNode.Enabled;
}
...
```

## Usage
Use **W** and **S** to move forward and backward.

Use **A** and **D** to move left and right.

Use **Q** and **E** to move up and down.

Roll the **scroll wheel** to increase and decrease movement speed.

Press **shift** to move faster, and hold **alt** to move slower.

Hold down the **right mouse button** to rotate the camera.
There's a slider in the editor to control mouse sensitivity.

## License
[MIT](https://opensource.org/licenses/MIT).
