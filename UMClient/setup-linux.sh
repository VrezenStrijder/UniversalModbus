#!/bin/bash

echo "设置Linux串口调试工具..."

# 检查用户是否在dialout组中
if ! groups $USER | grep -q '\bdialout\b'; then
    echo "将用户添加到dialout组..."
    sudo usermod -a -G dialout $USER
    echo "请注销并重新登录以使组权限生效"
fi

# 检查常见串口设备权限
echo "检查串口设备权限..."
for device in /dev/ttyUSB* /dev/ttyACM* /dev/ttyS*; do
    if [ -e "$device" ]; then
        echo "发现设备: $device"
        ls -l "$device"
    fi
done

# 设置应用程序权限
if [ -f "./UMClient" ]; then
    chmod +x ./UMClient
    echo "应用程序权限已设置"
fi

echo "设置完成！"
echo "如果这是首次运行，请注销并重新登录，然后运行: ./UMClient"
