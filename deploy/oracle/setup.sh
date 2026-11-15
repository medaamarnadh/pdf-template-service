#!/usr/bin/env bash
# One-time setup on a fresh Oracle Always Free Ubuntu 22.04/24.04 Arm VM. Run as the default 'ubuntu' user.
set -euo pipefail
sudo apt-get update -y && sudo apt-get install -y ca-certificates curl gnupg
# Docker (official repo)
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | sudo tee /etc/apt/sources.list.d/docker.list >/dev/null
sudo apt-get update -y && sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
sudo usermod -aG docker "$USER"
# Oracle images ship with iptables rules that drop 80/443; open them (the cloud Security List must allow them too)
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 80  -j ACCEPT
sudo iptables -I INPUT 6 -m state --state NEW -p tcp --dport 443 -j ACCEPT
sudo netfilter-persistent save 2>/dev/null || sudo apt-get install -y iptables-persistent
# App directory
mkdir -p ~/pdf-template-service/data
echo "Docker installed. Log out and back in (docker group), then: cd ~/pdf-template-service && docker compose up -d"
