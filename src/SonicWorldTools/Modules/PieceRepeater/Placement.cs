using UnityEngine;

namespace SonicWorldTools.Modules.PieceRepeater
{
internal struct Placement
	{
		internal Vector3 Position;
		internal Quaternion Rotation;

		internal Placement(Vector3 position, Quaternion rotation)
		{
			Position = position;
			Rotation = rotation;
		}
	}
}
