#!/bin/bash -e

if [ ! -e $HOME/oracle-xe-*.rpm ]
then
wget -nv https://raw.githubusercontent.com/Vincit/travis-oracledb-xe/master/packages/oracle-xe-11.2.0-1.0.x86_64.rpm.zip.aa
wget -nv https://raw.githubusercontent.com/Vincit/travis-oracledb-xe/master/packages/oracle-xe-11.2.0-1.0.x86_64.rpm.zip.ab
wget -nv https://raw.githubusercontent.com/Vincit/travis-oracledb-xe/master/packages/oracle-xe-11.2.0-1.0.x86_64.rpm.zip.ac
wget -nv https://raw.githubusercontent.com/Vincit/travis-oracledb-xe/master/packages/oracle-xe-11.2.0-1.0.x86_64.rpm.zip.ad
cat oracle-xe-11.2.0-1.0.x86_64.rpm.zip.* > $HOME/oracle-xe-11.2.0-1.0.x86_64.rpm.zip
export ORACLE_FILE="oracle-xe-11.2.0-1.0.x86_64.rpm.zip"
unzip -j "$(basename $ORACLE_FILE)" "*/*.rpm"
mv oracle-*/rpm $HOME/
fi


export ORACLE_HOME="/u01/app/oracle/product/11.2.0/xe"
export ORACLE_SID=XE

# make sure that hostname is found from hosts (or oracle installation will fail)
ping -c1 $(hostname) || echo 127.0.0.1 $(hostname) | sudo tee -a /etc/hosts

# Following Oracle Database Express Edition installer is from 2016-09-06
# https://github.com/cbandy/travis-oracle/blob/master/install.sh
#
# Copyright (c) 2013, Christopher Bandy
#
# Permission to use, copy, modify, and/or distribute this software for any purpose with or without fee is hereby granted,
# provided that the above copyright notice and this permission notice appear in all copies.
#
# THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED
# WARRANTIES OF MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR ANY SPECIAL, DIRECT, INDIRECT, OR
# CONSEQUENTIAL DAMAGES OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION OF CONTRACT,
# NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.

cd "$(dirname "$(readlink -f "$0")")"

df -B1 /dev/shm | awk 'END { if ($1 != "shmfs" && $1 != "tmpfs" || $2 < 2147483648) exit 1 }' ||
  ( sudo rm -r /dev/shm && sudo mkdir /dev/shm && sudo mount -t tmpfs shmfs -o size=2G /dev/shm )

test -f /sbin/chkconfig ||
  ( echo '#!/bin/sh' | sudo tee /sbin/chkconfig > /dev/null && sudo chmod u+x /sbin/chkconfig )

sudo mkdir -p /var/lock/subsys

sudo rpm --install --nodeps --nopre $HOME/oracle-xe-*.rpm

echo 'OS_AUTHENT_PREFIX=""' | sudo tee -a "$ORACLE_HOME/config/scripts/init.ora" > /dev/null
sudo usermod -aG dba $USER

( echo ; echo ; echo travis ; echo travis ; echo n ) | sudo AWK='/usr/bin/awk' /etc/init.d/oracle-xe configure

"$ORACLE_HOME/bin/sqlplus" -L -S / AS SYSDBA <<SQL
CREATE USER travis IDENTIFIED BY travis;
GRANT CONNECT, RESOURCE TO travis;
GRANT EXECUTE ON SYS.DBMS_LOCK TO travis;
SQL
