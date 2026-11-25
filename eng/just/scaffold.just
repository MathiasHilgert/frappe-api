set working-directory := "../.."

# Skeletons of what a use case repeats, from the templates in eng/templates; each prints its next steps (TODOs it cannot guess)

# A command, its handler and a failing unit test: just scaffold command Orders Shipments ShipOrder
command module area name:
    eng/scaffold/scaffold.sh command {{ module }} {{ area }} {{ name }}

# A query, its handler and a failing unit test: just scaffold query Orders Orders GetOrder
query module area name:
    eng/scaffold/scaffold.sh query {{ module }} {{ area }} {{ name }}

# An error: its code, its static error and its three messages. kind: notfound | forbidden | conflict | invalid | rule
error module code kind en es pt_br const="":
    eng/scaffold/scaffold.sh error {{ module }} {{ code }} {{ kind }} {{ quote(en) }} {{ quote(es) }} {{ quote(pt_br) }} {{ const }}

# An integration event (Contracts), its domain event and the translator arm: just scaffold event Orders Orders/OrderRenamed "OrderId:Guid,Name:string"
event module target fields:
    eng/scaffold/scaffold.sh event {{ module }} {{ target }} {{ quote(fields) }}

# An endpoint mapping: just scaffold endpoint Orders Shipments ShipOrder put /orders/{orderId}/shipment
endpoint module area name verb route:
    eng/scaffold/scaffold.sh endpoint {{ module }} {{ area }} {{ name }} {{ verb }} {{ quote(route) }}

# A CRUD skeleton: aggregate, mapping, repository and read port, create/get/list/update/delete, endpoints, Bruno requests: just scaffold crud Orders Order
crud module entity plural="":
    eng/scaffold/scaffold.sh crud {{ module }} {{ entity }} {{ plural }}

# A Bruno request with the next free seq of its folder: just scaffold bruno orders "Ship the order (200)" put /v1/orders/x/shipment
bruno folder name method url status="200":
    eng/scaffold/scaffold.sh bruno {{ folder }} {{ quote(name) }} {{ method }} {{ quote(url) }} {{ status }}
