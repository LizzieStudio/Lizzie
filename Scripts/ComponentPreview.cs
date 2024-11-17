using Godot;
using System;
using System.Collections.Generic;

public partial class ComponentPreview : Panel
{
	private Node3D _parentNode;
	public override void _Ready()
	{
		_parentNode = GetNode<Node3D>("SubViewportContainer/SubViewport/Node3D");
	}

	public override void _Process(double delta)
	{
		if (_component != null)
		{
			_component.Rotation += new Vector3(0,(float)delta, 0);
		}
	}

	private VisualComponentBase _component;

	private bool _componentActive;
	
	public void SetComponent(VisualComponentBase component, Vector3 rotation)
	{
		if (_componentActive)
		{
			ClearComponent();
		}

		_component = component;
		_componentActive = true;
		_component.Rotation = rotation;
		_parentNode.AddChild(_component);
	}

	public void ClearComponent()
	{
		if (_component == null) return;
		_component.QueueFree();
		_component = null;
		_componentActive = false;
	}

	public void SetComponentVisibility(bool visibility)
	{
		if (_component == null) return;
		_component.Visible = visibility;
	}

	public void Build(Dictionary<string, object> parameters)
	{
		if (_component != null)
		{
			_component.Build(parameters);
		}
	}
}
