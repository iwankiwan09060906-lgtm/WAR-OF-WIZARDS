"""
WAR OF WIZARDS - 3D Gesture Visualization Tool
File: WAR-OF-WIZARDS/AI/Tools/visualization.py

Visualizes 3D magic rune trajectories in 3D coordinate space.
"""

import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d import Axes3D
import numpy as np


def plot_3d_gesture(points: np.ndarray, title: str = "3D Gesture Trajectory", save_path: str = None):
    """
    Renders a 3D plot of a gesture trajectory.
    points: array of shape (N, 3) where columns are X, Y, Z
    """
    fig = plt.figure(figsize=(8, 7))
    ax = fig.add_subplot(111, projection='3d')

    x, y, z = points[:, 0], points[:, 1], points[:, 2]

    # Plot full path with gradient-like alpha
    ax.plot(x, y, z, color='#a855f7', linewidth=3, alpha=0.85, label='Spell Stroke')

    # Start Point (Emerald Green)
    ax.scatter([x[0]], [y[0]], [z[0]], color='#10b981', s=120, edgecolors='white', linewidth=2, label='Start')

    # End Point (Crimson Red)
    ax.scatter([x[-1]], [y[-1]], [z[-1]], color='#ef4444', s=120, edgecolors='white', linewidth=2, label='End')

    # Direction arrow along mid-path
    mid = len(points) // 2
    if mid + 1 < len(points):
        ax.quiver(
            x[mid], y[mid], z[mid],
            x[mid + 1] - x[mid], y[mid + 1] - y[mid], z[mid + 1] - z[mid],
            color='#38bdf8', length=0.2, normalize=True
        )

    ax.set_title(title, fontsize=14, pad=15, fontweight='bold')
    ax.set_xlabel('X (Horizontal)')
    ax.set_ylabel('Y (Vertical / Height)')
    ax.set_zlabel('Z (Depth / Forward)')
    ax.legend(loc='upper right')

    # Keep bounding box cubic
    ax.set_xlim([-1.2, 1.2])
    ax.set_ylim([-1.2, 1.2])
    ax.set_zlim([-1.2, 1.2])

    if save_path:
        plt.savefig(save_path, bbox_inches='tight', dpi=150)
        print(f"Plot saved to {save_path}")
    else:
        plt.show()

    plt.close()


if __name__ == '__main__':
    # Sample visualization test with a spiral/circle
    t = np.linspace(0, 2 * np.pi, 64)
    sample = np.column_stack([np.cos(t), np.sin(t), t / 10.0])
    plot_3d_gesture(sample, title="Test Shield Rune Circle", save_path=None)
