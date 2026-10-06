#!/usr/bin/env bash
# Deletes every AWS resource tagged stackbench, in case a run was killed before its own cleanup.
# Uses the AWS CLI's current profile and region.
set -euo pipefail

instances=$(aws ec2 describe-instances --filters Name=tag-key,Values=stackbench \
  Name=instance-state-name,Values=pending,running,stopping,stopped \
  --query 'Reservations[].Instances[].InstanceId' --output text)
if [ -n "$instances" ]; then
  echo "Terminating $instances"
  aws ec2 terminate-instances --instance-ids $instances > /dev/null
  aws ec2 wait instance-terminated --instance-ids $instances
fi
for sg in $(aws ec2 describe-security-groups --filters Name=tag-key,Values=stackbench --query 'SecurityGroups[].GroupId' --output text); do
  echo "Deleting security group $sg"
  aws ec2 delete-security-group --group-id "$sg"
done
for key in $(aws ec2 describe-key-pairs --filters Name=tag-key,Values=stackbench --query 'KeyPairs[].KeyName' --output text); do
  echo "Deleting key pair $key"
  aws ec2 delete-key-pair --key-name "$key" > /dev/null
done
echo "No stackbench resources left."
