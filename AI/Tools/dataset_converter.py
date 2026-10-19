"""
WAR OF WIZARDS - 3D Gesture Dataset Converter & Normalizer
File: WAR-OF-WIZARDS/AI/Tools/dataset_converter.py

Provides 3D stroke normalization (64 points, centering, aspect-preserving scaling)
used by fetch_dataset.py (server/DB samples -> training dataset).
"""

import numpy as np

TARGET_POINTS = 64


def resample_3d_trajectory(points: np.ndarray, num_points: int = TARGET_POINTS) -> np.ndarray:
    """
    Resamples an array of 3D points (N, 3) along its arc length to exactly `num_points` equidistant points.
    """
    if len(points) < 2:
        return np.repeat(points[:1], num_points, axis=0) if len(points) > 0 else np.zeros((num_points, 3))

    # Calculate cumulative arc length
    diffs = np.diff(points, axis=0)
    dists = np.linalg.norm(diffs, axis=1)
    cum_dists = np.concatenate(([0], np.cumsum(dists)))
    total_length = cum_dists[-1]

    if total_length == 0:
        return np.repeat(points[:1], num_points, axis=0)

    # Equidistant distances along arc length
    target_dists = np.linspace(0, total_length, num_points)

    # Interpolate x, y, z
    resampled = np.zeros((num_points, 3), dtype=np.float32)
    for dim in range(3):
        resampled[:, dim] = np.interp(target_dists, cum_dists, points[:, dim])

    return resampled


def normalize_3d_stroke(points: np.ndarray, num_points: int = TARGET_POINTS) -> np.ndarray:
    """
    Normalizes a 3D gesture trajectory:
    1. Resamples to `num_points` (default 64)
    2. Centers at (0, 0, 0)
    3. Scales bounding box maximum extent to [-1, 1]
    Output shape: (num_points, 3)
    """
    resampled = resample_3d_trajectory(points, num_points)

    # Centering (translate centroid to origin)
    centroid = np.mean(resampled, axis=0)
    centered = resampled - centroid

    # Scaling (find max absolute dimension to preserve 3D proportions)
    max_extent = np.max(np.abs(centered))
    if max_extent > 1e-6:
        normalized = centered / max_extent
    else:
        normalized = centered

    return normalized.astype(np.float32)


if __name__ == '__main__':
    # Unit test with synthetic circular stroke
    theta = np.linspace(0, 2 * np.pi, 100)
    raw_circle = np.column_stack([np.cos(theta), np.sin(theta), np.zeros_like(theta)])
    norm_circle = normalize_3d_stroke(raw_circle, TARGET_POINTS)
    assert norm_circle.shape == (64, 3), f"Wrong shape: {norm_circle.shape}"
    print(f"Dataset converter test pass: shape {norm_circle.shape}, range [{norm_circle.min():.2f}, {norm_circle.max():.2f}]")
