#!/bin/bash
raspivid -vf -o -| tee $1 | rclone --config /etc/rclone/rclone.conf rcat OneDrive:Cam1/$2
