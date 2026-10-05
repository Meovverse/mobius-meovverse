Mobius Meovverse 内部协作指南

这是一个小组内部的游戏项目。  
无论你之前有没有用过 Git，请先完整读一遍本文，再动手写代码。

一、这个项目是什么

- 项目名：Mobius Meovverse
- 类型：小组内部游戏项目
- 引擎 / 技术栈：godot

  
二、准备工作（每人只需做一次）

1. 安装 Git

- Windows：去 https://git-scm.com/download/win 下载安装，一路默认即可。
- Mac：终端运行 git --version，如果没有，按提示安装。
- 安装后打开 Git Bash（Windows）或终端（Mac/Linux）。

2. 配置 Git 用户名和邮箱

打开 Git Bash，执行：

git config --global user.name "你的用户名"
git config --global user.email "你的邮箱"

3. 配置 SSH（重要：必须走 443 端口）

很多公司、学校或地区网络会封掉 22 端口，导致连不上 GitHub。请按下面步骤配置 443 端口。

第一步：生成 SSH 密钥（如果已经有，跳过）

ssh-keygen -t ed25519 -C "你的邮箱"

一路回车即可。

第二步：把公钥添加到 GitHub

cat ~/.ssh/id_ed25519.pub

复制输出的全部内容，打开 GitHub → 右上角头像 → Settings → SSH and GPG keys → New SSH key，粘贴保存。

第三步：让 SSH 走 443 端口

在 Git Bash 中执行：

mkdir -p ~/.ssh
cat >> ~/.ssh/config <<'EOF'
Host github.com
  Hostname ssh.github.com
  Port 443
  User git
EOF

第四步：测试

ssh -T git@github.com

看到 Hi 你的用户名! You've successfully authenticated... 就算成功。

4. 获取仓库权限

联系管理员，把你的 GitHub 账号加入 Meovverse 组织，并给 mobius-meovverse 仓库的 Write 权限。  
没有 Write 权限时，推送会报 Write access to repository not granted。

三、克隆仓库到本地

git clone git@github.com:Meovverse/mobius-meovverse.git
cd mobius-meovverse

如果你之前用 HTTPS 克隆过，建议改成 SSH：
git remote set-url origin git@github.com:Meovverse/mobius-meovverse.git

四、日常开发流程（最重要，请照做）

铁律：永远不要直接修改 main 分支！

main 是稳定分支，所有人都通过 功能分支 + Pull Request（PR） 来合并代码。

完整流程

1. 每次开始写代码前，先更新本地 main

git checkout main
git pull origin main

2. 创建自己的功能分支

分支名用 feat/你的用户名-功能 或 fix/你的用户名-问题：

git checkout -b feat/你的用户名-add-player-movement

3. 写代码

在你的分支上随便改，不影响别人。

4. 查看改了哪些文件

git status

5. 提交到本地

git add .
git commit -m "feat: 添加玩家移动"

提交信息规范见第六节。

6. 推送到远程

git push -u origin feat/你的用户名-add-player-movement

7. 在 GitHub 上创建 PR

推送成功后，终端会显示一个链接，或者你直接打开：

https://github.com/Meovverse/mobius-meovverse

会看到黄色提示条 Compare & pull request，点击它。

- base 选 main
- compare 选你刚推的分支
- 填写标题和描述，说明你改了什么、为什么改
- 点击 Create pull request

8. 等待审查

至少一位组员 review 后，才能合并。  
如果审查意见让你修改，不要关 PR，直接在本地同一个分支改，然后：

git add .
git commit -m "fix: 按审查意见调整"
git push

PR 会自动更新。

9. 合并后清理

PR 被合并后，可以删除本地和远程的功能分支：

git checkout main
git pull origin main
git branch -d feat/你的用户名-add-player-movement
git push origin --delete feat/你的用户名-add-player-movement

五、PR 规则

1. 一个 PR 只做一件事，不要混入无关修改。
2. 标题写清楚：feat: 添加跳跃、fix: 修复碰撞bug。
3. 描述里写：改了什么、为什么改、怎么测试。
4. 至少一位组员 approve 才能合并。
5. 不要提交：密钥、账号密码、大文件、编译产物（build/、node_modules/ 等）。
6. 如果 PR 超过 3 天没人 review，在群里 @ 一下。

六、提交信息规范

格式：类型: 简短描述

常用类型：

- feat: 新功能
- fix: 修复 bug
- docs: 文档
- chore: 杂项（配置、依赖等）
- refactor: 重构
- art: 美术资源

例子：

feat: 添加玩家二段跳
fix: 修复角色卡墙问题
docs: 更新 README 协作指南

七、常见问题

1. ssh: connect to host github.com port 22: Connection refused

说明 22 端口被封，按第二节配置 443 端口即可。

2. ERROR: Write access to repository not granted

SSH 已通，但你没有仓库写权限。联系管理员给你 Write 权限。

3. protected branch 或 pre-receive hook declined

你试图直接推 main。请创建功能分支再推。

4. 推送时提示冲突

先更新 main：

git checkout main
git pull origin main
git checkout 你的分支
git merge main

手动解决冲突后：

git add .
git commit -m "chore: 解决冲突"
git push

5. 我不小心改了 main 怎么办

git checkout main
git reset --hard origin/main

这会丢弃你本地 main 上未提交的修改，慎用。

八、资源文件规范

- 大文件（音频、视频、大图）请使用 Git LFS，或放在共享网盘，不要直接提交到仓库。
- 编译产物、缓存、临时文件不要提交。
- 提交前检查 git status，确认没有多余文件。

九、联系与求助


遇到 Git 问题不要硬扛，截图发群里，大家帮你解决。
