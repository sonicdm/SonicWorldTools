namespace SonicWorldTools.Core
{
	/// <summary>
	/// Selection bounds. Default is axis-aligned (min/max). When <see cref="IsOriented"/>,
	/// a Y-up OBB from three corners (A→B width edge, C depth/height).
	/// </summary>
	public partial struct SelectionVolume
	{
		public float MinX;

		public float MinY;

		public float MinZ;

		public float MaxX;

		public float MaxY;

		public float MaxZ;

		/// <summary>True when volume is a Y-up oriented box (three-corner flow).</summary>
		public bool IsOriented;

		public float OriginX;

		public float OriginY;

		public float OriginZ;

		/// <summary>Yaw radians: local +X on XZ = (cos, sin).</summary>
		public float Yaw;

		public float ExtX;

		public float ExtY;

		public float ExtZ;

		public float SizeX => IsOriented ? ExtX : MaxX - MinX;

		public float SizeY => IsOriented ? ExtY : MaxY - MinY;

		public float SizeZ => IsOriented ? ExtZ : MaxZ - MinZ;

		public static SelectionVolume FromCorners(
			float ax,
			float ay,
			float az,
			float bx,
			float by,
			float bz)
		{
			SelectionVolumeMath.Normalize(
				ax,
				ay,
				az,
				bx,
				by,
				bz,
				out float minX,
				out float minY,
				out float minZ,
				out float maxX,
				out float maxY,
				out float maxZ);

			return new SelectionVolume
			{
				MinX = minX,
				MinY = minY,
				MinZ = minZ,
				MaxX = maxX,
				MaxY = maxY,
				MaxZ = maxZ,
				IsOriented = false,
			};
		}

		/// <summary>
		/// A→B = width edge on XZ; C = depth (perp. on XZ) + height span (min/max Y).
		/// </summary>
		public static SelectionVolume FromThreePoints(
			float ax,
			float ay,
			float az,
			float bx,
			float by,
			float bz,
			float cx,
			float cy,
			float cz)
		{
			SelectionVolumeMath.OrientedFromThreePoints(
				ax,
				ay,
				az,
				bx,
				by,
				bz,
				cx,
				cy,
				cz,
				out float originX,
				out float originY,
				out float originZ,
				out float yaw,
				out float extX,
				out float extY,
				out float extZ,
				out float minX,
				out float minY,
				out float minZ,
				out float maxX,
				out float maxY,
				out float maxZ);

			return new SelectionVolume
			{
				MinX = minX,
				MinY = minY,
				MinZ = minZ,
				MaxX = maxX,
				MaxY = maxY,
				MaxZ = maxZ,
				IsOriented = true,
				OriginX = originX,
				OriginY = originY,
				OriginZ = originZ,
				Yaw = yaw,
				ExtX = extX,
				ExtY = extY,
				ExtZ = extZ,
			};
		}

		/// <summary>Expand every face by a uniform margin, preserving the volume's orientation.</summary>
		public SelectionVolume Expanded(float padding)
		{
			if (padding <= 0f)
			{
				return this;
			}

			if (!IsOriented)
			{
				return FromCorners(
					MinX - padding,
					MinY - padding,
					MinZ - padding,
					MaxX + padding,
					MaxY + padding,
					MaxZ + padding);
			}

			float cos = (float)System.Math.Cos(Yaw);
			float sin = (float)System.Math.Sin(Yaw);
			float expandedOriginX = OriginX - (padding * cos) + (padding * sin);
			float expandedOriginY = OriginY - padding;
			float expandedOriginZ = OriginZ - (padding * sin) - (padding * cos);
			float expandedX = ExtX + (padding * 2f);
			float expandedY = ExtY + (padding * 2f);
			float expandedZ = ExtZ + (padding * 2f);
			SelectionVolumeMath.WorldAabbFromOriented(
				expandedOriginX,
				expandedOriginY,
				expandedOriginZ,
				Yaw,
				expandedX,
				expandedY,
				expandedZ,
				out float minX,
				out float minY,
				out float minZ,
				out float maxX,
				out float maxY,
				out float maxZ);

			return new SelectionVolume
			{
				MinX = minX,
				MinY = minY,
				MinZ = minZ,
				MaxX = maxX,
				MaxY = maxY,
				MaxZ = maxZ,
				IsOriented = true,
				OriginX = expandedOriginX,
				OriginY = expandedOriginY,
				OriginZ = expandedOriginZ,
				Yaw = Yaw,
				ExtX = expandedX,
				ExtY = expandedY,
				ExtZ = expandedZ,
			};
		}

		/// <summary>Inclusive on all faces: a point on the boundary is inside.</summary>
		public bool Contains(float x, float y, float z)
		{
			if (IsOriented)
			{
				return SelectionVolumeMath.ContainsOriented(
					OriginX,
					OriginY,
					OriginZ,
					Yaw,
					ExtX,
					ExtY,
					ExtZ,
					x,
					y,
					z);
			}

			return SelectionVolumeMath.Contains(MinX, MinY, MinZ, MaxX, MaxY, MaxZ, x, y, z);
		}

		/// <summary>World AABB (enclosure). Safe for nearby-piece radius queries.</summary>
		public void GetWorldAabb(
			out float minX,
			out float minY,
			out float minZ,
			out float maxX,
			out float maxY,
			out float maxZ)
		{
			minX = MinX;
			minY = MinY;
			minZ = MinZ;
			maxX = MaxX;
			maxY = MaxY;
			maxZ = MaxZ;
		}
	}
}
